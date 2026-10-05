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

    // --- CW-H：交接處之外牆面由立面內的實體外牆供給（docs §4.2、決議 13）------------------------

    [Fact]
    public void A_rated_solid_wall_in_the_facade_supplies_the_but_clauses_length()
    {
        // 交接帶那一段立面不鋪嵌板，改以一道具時效的實體外牆表達（§4.2 建模要求）：但書的
        // 「交接處之外牆面長度」就是這道牆沿立面的水平長度。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) }, facades: new[] { Facade(4100, 5900) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(1800, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(60, junction.MinFireRating!.Minutes!.Value);
    }

    [Fact]
    public void The_facade_walls_the_run_was_measured_on_are_named_in_the_junction()
    {
        // 「這個 900 mm 是哪一段外牆」必須答得出來，而且答案不能是區劃牆自己：把 host 的 UniqueId
        // 塞進嵌板清單會讓 ReviewMarkup 把區劃牆塗紅（那是它明文不做的事）。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) }, facades: new[] { Facade(4100, 5900) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(new[] { "W-facade" }, junction.FacadeWallUniqueIds);
        Assert.DoesNotContain("W1", junction.PanelUniqueIds);
        Assert.Contains("W-facade", junction.SubjectUniqueIds);
    }

    [Fact]
    public void A_facade_wall_that_covers_only_part_of_the_junctions_height_supplies_nothing()
    {
        // 區劃牆通層 0–3600，實體外牆只做到 1900：火焰沿立面繞行的高度就是區劃牆在該處的高度，
        // 只封住其中一段等於留了缺口。供給 1800 會讓「牆做一半、旁邊照樣開著」過關。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) },
                facades: new[] { Facade(4100, 5900, top: 1900) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
        Assert.Empty(junction.FacadeWallUniqueIds);
    }

    [Fact]
    public void A_facade_wall_taller_than_the_junction_counts_in_full()
    {
        // 反面：牆比交接帶高沒有關係——一道通層具時效的實體外牆比 90 cm 帶更充分，不是更不充分。
        var host = new CompartmentWallObservation(
            "W1", new Point2D(5000, 3000), new Point2D(5000, -50), 1200, 2100,
            CurtainWallJunctionReferences.Article79, 60, "RC 牆 15cm");

        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { host },
                facades: new[] { Facade(4100, 5900, bottom: 0, top: StoreyMm) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(1800, junction.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void A_facade_wall_without_a_readable_rating_withholds_the_length()
    {
        // 型別沒填（或類別沒綁）防火檢討_設計防火時效：不得推定，也不得填 0——交由引擎判資料不足。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) },
                facades: new[] { Facade(4100, 5900, minutes: null) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Null(junction.ContinuousFireRatedLengthMm);
        Assert.Equal(ProvidedFireRatingKind.Missing, junction.MinFireRating!.Kind);
        Assert.Equal(0.0, junction.ProjectionDepthMm!.Value);
    }

    [Fact]
    public void A_facade_wall_below_the_required_rating_supplies_no_length()
    {
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) },
                facades: new[] { Facade(4100, 5900, minutes: 30) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
        Assert.Equal(30, junction.MinFireRating!.Minutes!.Value);
    }

    [Fact]
    public void A_compartment_wall_that_merely_reaches_a_glazed_facade_supplies_nothing()
    {
        // 這是決議 13 的守門測試：區劃牆抵到帷幕牆、自己還具 120 min 時效，但立面在那裡是玻璃。
        // 若容許以區劃牆自身的時效作答，任何具時效的區劃牆抵上玻璃帷幕牆都會自動合格，
        // 第79條第4項即形同虛設。這裡不是資料不足——外牆面是玻璃，那是事實。
        var junction = Single(Resolve(Set(Wall(Glazing()), hosts: new[] { Host(5000) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
        Assert.Null(junction.MinFireRating);
        Assert.Empty(junction.FacadeWallUniqueIds);
    }

    [Fact]
    public void A_rated_curtain_panel_no_longer_answers_the_horizontal_but_clause()
    {
        // 交點上是一片 60 min 的實板，達得到區劃牆要求的 60 min——舊決議 7 會供給 1800 mm。
        // 決議 13 起嵌板完全不參與 CW-H：但書的主詞是「該外牆構造」，嵌板（含認證防火玻璃）
        // 答不了（docs §9 記了這個代價）。
        var junction = Single(
            Resolve(Set(Wall(new[] { Panel("P-solid", 4100, 5900, 60) }), hosts: new[] { Host(5000) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
        Assert.Contains("P-solid", junction.PanelUniqueIds);
    }

    [Fact]
    public void A_wall_standing_behind_the_facade_is_not_part_of_it()
    {
        // 貼在帷幕牆背面而非共面的實體牆不算：橫向偏差 300 mm 超過 FacadePlaneToleranceMm。
        var behind = new FacadeWallObservation(
            "W-inside", new Point2D(4100, -300), new Point2D(5900, -300), 0, StoreyMm, "RC 牆 15cm",
            ProvidedFireRating.Rated(120, "120"));

        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) }, facades: new[] { behind })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
    }

    [Fact]
    public void A_wall_that_is_not_parallel_to_the_facade_is_not_part_of_it()
    {
        // 兩端都落在 150 mm 的面內公差裡，但斜了 5.7°，超過 FacadeAngleToleranceDeg：那是一道
        // 斜切進立面的牆，不是這一片立面本身。
        var askew = new FacadeWallObservation(
            "W-askew", new Point2D(4000, 100), new Point2D(6000, -100), 0, StoreyMm, "RC 牆 15cm",
            ProvidedFireRating.Rated(120, "120"));

        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) }, facades: new[] { askew })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
    }

    [Fact]
    public void Two_facade_walls_butted_together_are_one_continuous_run()
    {
        // 兩道牆對接處的接縫在 TouchToleranceMm 內：那是一片外牆，不是兩段。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4550, 5450)), hosts: new[] { Host(5000) },
                facades: new[] { Facade(4550, 5000, uniqueId: "W-left"), Facade(5000, 5450, uniqueId: "W-right") })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(new[] { "W-left", "W-right" }, junction.FacadeWallUniqueIds);
    }

    [Fact]
    public void A_gap_between_two_facade_walls_stops_the_run()
    {
        // 兩道牆之間空了 100 mm：累積停在缺口上，只量到交點所在那一道（否則會是 3000 mm）。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(3000, 6000)), hosts: new[] { Host(5400) },
                facades: new[] { Facade(3000, 4950, uniqueId: "W-left"), Facade(5050, 6000, uniqueId: "W-right") })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(950, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(new[] { "W-right" }, junction.FacadeWallUniqueIds);
    }

    [Fact]
    public void A_neighbouring_facade_wall_below_the_required_rating_stops_the_run()
    {
        // 案例 5 的實體外牆版：隔壁那道只有 30 min，達不到區劃牆要求的 60 min，累積停在交界上。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4550, 7000)), hosts: new[] { Host(5000) },
                facades: new[]
                {
                    Facade(4550, 5450, uniqueId: "W-rated"),
                    Facade(5450, 7000, minutes: 30, uniqueId: "W-30")
                })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(new[] { "W-rated" }, junction.FacadeWallUniqueIds);
    }

    [Fact]
    public void A_short_run_stopped_by_a_facade_wall_with_no_readable_rating_is_not_supplied()
    {
        // 量到 700 mm 就撞上一道讀不出時效的牆：那道牆若有時效就可能達 900 mm，判未符合是說了
        // 工具不知道的事。與嵌板路徑同一個輸入契約——量到的是「至少這麼長」。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4300, 7000)), hosts: new[] { Host(5000) },
                facades: new[]
                {
                    Facade(4300, 5000, uniqueId: "W-rated"),
                    Facade(5000, 7000, minutes: null, uniqueId: "W-unset")
                })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Null(junction.ContinuousFireRatedLengthMm);
        Assert.Equal(ProvidedFireRatingKind.Missing, junction.MinFireRating!.Kind);
        Assert.Contains("W-unset", junction.FacadeWallUniqueIds);
    }

    [Fact]
    public void A_run_is_the_sum_of_both_sides_rather_than_half_of_it_each()
    {
        // 案例 7：交點落在實體外牆的右緣，左側 900 mm、右側 0 mm — 採總和，不要求各半。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) },
                facades: new[] { Facade(4100, 5000) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.False(junction.IsDoubtful);
    }

    [Fact]
    public void A_junction_on_the_end_mullion_is_measured_on_the_facade_wall_that_runs_past_it()
    {
        // 區劃牆對齊帷幕牆端部的收邊豎框是常態做法，而但書問的是外牆面，不是帷幕牆的範圍：
        // 實體外牆延伸到帷幕牆之外的那一段照樣是外牆面，累積不截在帷幕牆的端點上。
        // 決議 13 起「交點被豎框佔著」不再需要特別處理——CW-H 根本不查嵌板。
        var junction = Single(
            Resolve(Set(Wall(new[] { Panel("P-glass", 600, WallLengthMm, 0) }), hosts: new[] { Host(0) },
                facades: new[] { Facade(-500, 500) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(1000, junction.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void A_facade_wall_overlapping_a_curtain_panel_is_manual_review()
    {
        // 把實體牆疊在玻璃嵌板前面而沒有把嵌板拿掉，是這個設計最容易踩的建模錯。模型對同一片
        // 外牆講了兩件互相矛盾的事，工具不替使用者選一個。
        var junction = Single(
            Resolve(Set(Wall(Glazing()), hosts: new[] { Host(5000) }, facades: new[] { Facade(4100, 5900) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.True(junction.IsDoubtful);
        Assert.Equal(CurtainWallJunctionDoubtKind.FacadeWallOverlapsPanel, junction.Doubt!.Kind);
        Assert.Equal(ReviewStatus.ManualReview, junction.Doubt.Status);
        Assert.Contains("W-facade", junction.Doubt.SubjectUniqueIds);
        Assert.Contains("P-glass", junction.Doubt.SubjectUniqueIds);
        Assert.Null(junction.ContinuousFireRatedLengthMm);
    }

    [Fact]
    public void A_facade_wall_answers_even_when_the_curtain_wall_is_built_one_storey_at_a_time()
    {
        // 決議 7 在實務模型上等於不成立的主因：帷幕牆逐層建，上下是兩個元素，防火帶跨在樓板上。
        // 實體外牆是獨立元素、不必屬於任一片帷幕牆，所以兩片都量得到同一道牆（docs §12 步驟 12）。
        var host = new CompartmentWallObservation(
            "W1", new Point2D(5000, 3000), new Point2D(5000, -50), 1200, 2400,
            CurtainWallJunctionReferences.Article79, 60, "RC 牆 15cm");
        var lower = new CurtainWallObservation(
            "CW-low", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1), OffsetMm, 0, 1800,
            GlazingExcept(4100, 5900, top: 1800), null, null, "帷幕牆 下段");
        var upper = new CurtainWallObservation(
            "CW-high", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1), OffsetMm, 1800, StoreyMm,
            GlazingExcept(4100, 5900, bottom: 1800), null, null, "帷幕牆 上段");

        var set = new CurtainWallObservationSet(
            Package, "LVL", "1F", 0, new[] { Zone() }, new[] { lower, upper }, new[] { host }, null,
            new[] { Facade(4100, 5900) }, new[] { 0.0, StoreyMm });

        var junctions = Resolve(set).Where(j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall).ToList();

        Assert.Equal(2, junctions.Count);
        Assert.All(junctions, j => Assert.Equal(1800, j.ContinuousFireRatedLengthMm!.Value, 3));
    }

    [Fact]
    public void A_host_between_two_stacked_curtain_walls_produces_no_cw_h_junction_at_all()
    {
        // 上一條的限制面（記在 §9）：交接處只在區劃牆的高程與某片帷幕牆重疊時才存在。區劃牆的
        // 高程帶剛好整段落在上下兩片帷幕牆之間的縫裡時，CW-H 整列消失，不是判未符合。
        var host = new CompartmentWallObservation(
            "W1", new Point2D(5000, 3000), new Point2D(5000, -50), 1200, 2100,
            CurtainWallJunctionReferences.Article79, 60, "RC 牆 15cm");
        var lower = new CurtainWallObservation(
            "CW-low", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1), OffsetMm, 0, 1200,
            new[] { Panel("P-low", 0, WallLengthMm, 0, bottom: 0, top: 1200) }, null, null, "帷幕牆 下段");
        var upper = new CurtainWallObservation(
            "CW-high", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1), OffsetMm, 2100, StoreyMm,
            new[] { Panel("P-high", 0, WallLengthMm, 0, bottom: 2100, top: StoreyMm) }, null, null, "帷幕牆 上段");

        var set = new CurtainWallObservationSet(
            Package, "LVL", "1F", 0, new[] { Zone() }, new[] { lower, upper }, new[] { host }, null,
            new[] { Facade(4100, 5900, bottom: 1200, top: 2100) }, new[] { 0.0, StoreyMm });

        Assert.DoesNotContain(Resolve(set), j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall);
    }

    [Fact]
    public void The_panels_the_band_covers_are_recorded_and_left_out_of_the_other_exterior_wall()
    {
        // CW-H 的 junction.panels 自決議 13 起是「交接帶涵蓋的嵌板」而非「被量到的嵌板」：帶長由
        // 實體外牆供給，但這些嵌板仍要拿來扣 CW-O 並在未符合時塗紅（docs §7.1）。
        var wall = Wall(new[]
        {
            Panel("P-in-band", 4100, 4550, 0),
            Panel("P-outside", 6400, WallLengthMm, 30)
        });

        var junctions = Resolve(Set(wall, hosts: new[] { Host(5000) }, facades: new[] { Facade(4550, 5900) }));

        var junction = Single(junctions, CurtainWallJunctionKind.WallToCurtainWall);
        Assert.Equal(new[] { "P-in-band" }, junction.PanelUniqueIds);

        var other = Single(junctions, CurtainWallJunctionKind.CurtainPanelOther);
        Assert.Equal(new[] { "P-outside" }, other.PanelUniqueIds);
    }

    /// <summary>
    /// 案例 15：`防火檢討_設計防火時效` 根本沒綁在該類別上。The adapter reads that as an absent
    /// parameter, which is the same 「不知道」 as a blank one — both 90 cm 但書 withhold their
    /// measurement rather than reading it as 0, so the engine lands on 資料不足 and not 未符合. CW-H
    /// reads it off the solid exterior wall and CW-V off the panels, but it is the same parameter and
    /// the same answer.
    /// </summary>
    [Fact]
    public void An_unbound_fire_rating_parameter_withholds_both_the_length_and_the_height()
    {
        var unbound = ReviewInputAssembler.Rating(ParameterReading.Absent);
        Assert.Equal(ProvidedFireRatingKind.Missing, unbound.Kind);

        var horizontal = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000) },
                facades: new[]
                {
                    new FacadeWallObservation("W-facade", new Point2D(4100, 0), new Point2D(5900, 0),
                        0, StoreyMm, "RC 牆 15cm", unbound)
                })),
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
    public void The_clause_the_compartment_comes_from_is_carried_through()
    {
        // 案例 9：第83條所生的區劃牆，證據要分得出來（docs §2.5）。
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)),
                hosts: new[] { Host(5000, reference: CurtainWallJunctionReferences.Article83) },
                facades: new[] { Facade(4100, 5900) })),
            CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(CurtainWallJunctionReferences.Article83, junction.HostLegalReference);
        Assert.Equal(1800, junction.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void A_host_with_no_required_rating_withholds_the_run_rather_than_measuring_the_facade()
    {
        var junction = Single(
            Resolve(Set(Wall(GlazingExcept(4100, 5900)), hosts: new[] { Host(5000, required: null) },
                facades: new[] { Facade(4100, 5900) })),
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
    public void A_band_split_by_a_grid_line_with_both_sides_rated_is_manual_review()
    {
        // 案例 8：樓板上下各 450 mm 的實板都是 60 min，合計已達 900 mm，中間卻有一條水平 grid line。
        // 工具不跨越 grid line 累積（§4.1），所以這裡不判符合，而是指出那條線要使用者確認是否為
        // 真實構造斷點。案例 8b（刪掉它、嵌板連續後量得 900 mm）就是本節第一條測試。
        var set = SpandrelSet(
            new[]
            {
                Panel("S-low", 0, WallLengthMm, 60, bottom: StoreyMm - 450, top: StoreyMm),
                Panel("S-high", 0, WallLengthMm, 60, bottom: StoreyMm, top: StoreyMm + 450),
                Panel("G-low", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 450),
                Panel("G-high", 0, WallLengthMm, 0, bottom: StoreyMm + 450, top: StoreyMm * 2)
            },
            gridLines: new[] { Grid("G-1", CurtainGridLineDirection.Horizontal, StoreyMm) });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.True(junction.IsDoubtful);
        Assert.Equal(CurtainWallJunctionDoubtKind.SplitByGridLine, junction.Doubt!.Kind);
        Assert.Equal(ReviewStatus.ManualReview, junction.Doubt.Status);
        Assert.Contains("G-1", junction.Doubt.Message);
        Assert.Contains("層間帶", junction.Doubt.Message);
        Assert.Contains("G-1", junction.Doubt.SubjectUniqueIds);
    }

    [Fact]
    public void An_unrated_neighbour_is_a_real_break_and_the_band_still_reports()
    {
        // 案例 8c：grid line 一側是 60 min 實板、另一側是明確無時效的玻璃 — 真實斷點，不是多餘的
        // grid line，依實測的 450 mm 判定。
        var set = SpandrelSet(
            new[]
            {
                Panel("S-low", 0, WallLengthMm, 60, bottom: StoreyMm - 450, top: StoreyMm),
                Panel("G-low", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 450),
                Panel("G-high", 0, WallLengthMm, 0, bottom: StoreyMm, top: StoreyMm * 2)
            },
            gridLines: new[] { Grid("G-1", CurtainGridLineDirection.Horizontal, StoreyMm) });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.False(junction.IsDoubtful);
        Assert.Equal(450, junction.ContinuousFireRatedHeightMm!.Value, 3);
    }

    [Fact]
    public void An_unprotected_opening_stops_the_band_where_it_sits()
    {
        // 案例 6：取樣點就落在未受防護的可開啟嵌板上 — 累積在開口處中斷。
        var set = SpandrelSet(new[]
        {
            Panel("W-window", 0, WallLengthMm, 60, bottom: StoreyMm - 450, top: StoreyMm + 450,
                isOpening: true, protection: ProvidedFireProtection.No("0")),
            Panel("G-low", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 450),
            Panel("G-high", 0, WallLengthMm, 0, bottom: StoreyMm + 450, top: StoreyMm * 2)
        });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedHeightMm!.Value);
        Assert.True(junction.HasUnprotectedOpening);
    }

    [Fact]
    public void A_protected_opening_in_the_band_is_not_a_break()
    {
        var set = SpandrelSet(new[]
        {
            Panel("W-door", 0, WallLengthMm, 60, bottom: StoreyMm - 450, top: StoreyMm + 450,
                isOpening: true, protection: ProvidedFireProtection.Yes("1")),
            Panel("G-low", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 450),
            Panel("G-high", 0, WallLengthMm, 0, bottom: StoreyMm + 450, top: StoreyMm * 2)
        });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(900, junction.ContinuousFireRatedHeightMm!.Value, 3);
        Assert.False(junction.HasUnprotectedOpening);
    }

    [Fact]
    public void A_band_cut_short_by_the_curtain_walls_own_top_is_not_supplied()
    {
        // 立面在這裡接到另一片牆，工具沒讀到它 — 那是不知道，不是不足。CW-H 自決議 13 起不受這一條
        // 影響：實體外牆是獨立元素，延伸到帷幕牆之外的那一段照樣讀得到。
        var floor = new CompartmentFloorObservation("F1",
            new[] { Rectangle(-500, -50, WallLengthMm + 500, 8000) }, StoreyMm, 60);
        var panels = new[]
        {
            Panel("G-low", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 300),
            Panel("S-top", 0, WallLengthMm, 60, bottom: StoreyMm - 300, top: StoreyMm + 450)
        };
        var set = new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() },
            new[] { Wall(panels, top: StoreyMm + 450) },
            compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Null(junction.ContinuousFireRatedHeightMm);
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
    public void The_storey_aboves_own_curtain_wall_is_not_a_vertical_space_of_this_storey()
    {
        // 實機（帷幕牆 290096，FL3 → FL4）：檢討 FL2 時讀取範圍上下各放寬 900 mm，上一層那片一層高的
        // 帷幕牆也被讀進來。它站在 FL3 上、沒有穿過 FL3，FL2 的封包裡沒有 FL3 樓板不代表它是挑空。
        var wall = new CurtainWallObservation("CW-above", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1),
            OffsetMm, StoreyMm * 2, StoreyMm * 3,
            new[] { Panel("P-above", 0, WallLengthMm, 0, bottom: StoreyMm * 2, top: StoreyMm * 3) },
            typeName: "帷幕牆 1");
        var set = new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() }, new[] { wall },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2, StoreyMm * 3 });

        Assert.DoesNotContain(Resolve(set), j => j.Doubt?.Kind == CurtainWallJunctionDoubtKind.VerticalCompartmentSpace);
    }

    [Fact]
    public void A_vertical_space_counts_the_storeys_from_the_curtain_walls_own_base()
    {
        // 自 1F 起連跨三層的帷幕牆，在 2F 的封包裡讀到：跨幾層從牆底算，不從本層算。
        var set = new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() },
            new[] { Wall(Glazing(top: StoreyMm * 3), top: StoreyMm * 3) },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2, StoreyMm * 3 });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(CurtainWallJunctionDoubtKind.VerticalCompartmentSpace, junction.Doubt!.Kind);
        Assert.Contains("連跨 3 個樓層", junction.Doubt.Message);
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
    public void Every_panel_is_answered_as_other_exterior_wall_by_exactly_one_storey()
    {
        // 逐層建模的兩片帷幕牆（1F：0–3600、2F：3600–7200）。讀取範圍上下各放寬 900 mm，所以兩個封包
        // 都讀得到兩片牆；第79條之4 每片嵌板只能由它所在的那一層作答一次。
        var lower = new CurtainWallObservation("CW-1F", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1),
            OffsetMm, 0, StoreyMm, new[] { Glass("P-1F", 0, WallLengthMm, ProvidedFireProtection.Yes("是")) }, typeName: "帷幕牆 1");
        var upper = new CurtainWallObservation("CW-2F", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1),
            OffsetMm, StoreyMm, StoreyMm * 2,
            new[] { Glass("P-2F", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), bottom: StoreyMm, top: StoreyMm * 2) },
            typeName: "帷幕牆 1");
        var levels = new[] { 0.0, StoreyMm, StoreyMm * 2 };

        IEnumerable<string> OtherPanels(string storey, double elevation) =>
            Resolve(new CurtainWallObservationSet(Package, "LVL-" + storey, storey, elevation,
                    new[] { Zone() }, new[] { lower, upper }, levelElevationsMm: levels))
                .Where(j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther)
                .SelectMany(j => j.PanelUniqueIds);

        Assert.Equal(new[] { "P-1F" }, OtherPanels("1F", 0).ToArray());
        Assert.Equal(new[] { "P-2F" }, OtherPanels("2F", StoreyMm).ToArray());
    }

    [Fact]
    public void The_storey_aboves_curtain_wall_makes_no_row_at_all_in_this_storey()
    {
        // 實機（帷幕牆 290096）：FL2 的封包讀到 FL3 那片一層高的帷幕牆，它在 FL2 不產出任何一列。
        var wall = new CurtainWallObservation("CW-above", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, -1),
            OffsetMm, StoreyMm * 2, StoreyMm * 3,
            new[] { Glass("P-above", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), bottom: StoreyMm * 2, top: StoreyMm * 3) },
            typeName: "帷幕牆 1");
        var set = new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() }, new[] { wall },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2, StoreyMm * 3 });

        Assert.Empty(Resolve(set));
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

    // --- 交點落在實體外牆連續段上（docs §4.2「交點落在實體外牆上」、§10 案例 26–33、決議 14）--------

    [Fact]
    public void Case26_a_crossing_on_the_solid_wall_between_two_curtain_walls_is_still_a_junction()
    {
        // §4.2「建模要求」：防火帶以實體牆元素取代該段帷幕牆，帷幕牆因此被切成兩片，交點落在兩片
        // 之間——也就是各自定位線的延長線上。決議 13 的實作只認段內，這一列會整個消失。
        var junction = Assert.Single(WallJunctions(SplitFacade(4100, 5000, new[] { Host(4550) })));

        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(new[] { "W-facade" }, junction.FacadeWallUniqueIds);
        Assert.Equal(0.0, junction.ProjectionDepthMm!.Value);
        Assert.Null(junction.Doubt);
    }

    [Fact]
    public void Case27_a_solid_wall_one_millimetre_short_of_900_measures_one_millimetre_short()
    {
        var junction = Assert.Single(WallJunctions(SplitFacade(4100, 4999, new[] { Host(4550) })));

        Assert.Equal(899, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(new[] { "W-facade" }, junction.FacadeWallUniqueIds);
    }

    [Fact]
    public void Case28_the_run_has_a_curtain_wall_at_each_end_and_only_the_lower_corner_one_owns_it()
    {
        // 兩片帷幕牆都求得到同一個交點。擁有者必須與畫牆方向、與讀取順序都無關，否則同一個交接處
        // 會出兩列、兩個檢討圖號：判準是相接端點的 (X, Y) 字典序，不是 UniqueId、也不是沿軸較低側。
        var named = Assert.Single(WallJunctions(SplitFacade(4100, 5000, new[] { Host(4550) },
            leftUniqueId: "CW-z-left", rightUniqueId: "CW-a-right")));
        var swapped = Assert.Single(WallJunctions(SplitFacade(4100, 5000, new[] { Host(4550) },
            leftUniqueId: "CW-a-left", rightUniqueId: "CW-z-right")));

        Assert.Equal("CW-z-left", named.CurtainWallUniqueId);
        Assert.Equal("CW-a-left", swapped.CurtainWallUniqueId);
        Assert.Equal(900, named.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void Case29_a_run_that_does_not_reach_the_curtain_wall_is_no_junction_rather_than_人工覆核()
    {
        // 5 mm 的縫：那裡沒有交接處，工具不替使用者橋接立面上的縫隙（§9）。判 ManualReview 會要求
        // 使用者覆核一件不存在的事。
        var junctions = Resolve(SplitFacade(4100, 5000, new[] { Host(4550) }, seamMm: 5));

        Assert.DoesNotContain(junctions, j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall);
    }

    [Fact]
    public void Case30_a_compartment_wall_lying_in_the_facade_supplies_length_but_produces_no_junction_of_its_own()
    {
        // 立面內的實體外牆自己也躺在區劃邊界上（實機模型的 RC 牆就是這樣）。它與帷幕牆平行，求不到
        // 交點；決議 14 之前會掉進「最近端點」退路，回報「端點距帷幕牆 0 mm，超過搜尋公差 300 mm」。
        var inFacade = new CompartmentWallObservation("W-in-facade",
            new Point2D(4100, 0), new Point2D(5000, 0), 0, StoreyMm, CurtainWallJunctionReferences.Article79, 60);

        var junction = Assert.Single(WallJunctions(Set(Wall(GlazingExcept(4100, 5000)),
            hosts: new[] { Host(4550), inFacade }, facades: new[] { Facade(4100, 5000) })));

        Assert.Equal("W1", junction.HostUniqueId);
        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
    }

    [Fact]
    public void Case31_the_junction_band_is_the_compartment_wall_met_with_the_curtain_walls_own_elevations()
    {
        // 區劃牆通層 0–3600，帷幕牆與實體外牆自樓板面上方 450 起算：交接帶是交集 450–3600。以區劃牆
        // 全高當門檻會要求實體外牆往下長進樓板，那 450 是樓板邊緣，屬第 79 條之 3 由 CW-V 回答。
        var junction = Assert.Single(WallJunctions(SplitFacade(4100, 5000, new[] { Host(4550) },
            facadeBottomMm: 450, curtainBaseMm: 450)));

        Assert.Equal(900, junction.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(450, junction.Placement!.BottomElevationMm, 3);
        Assert.Equal(StoreyMm, junction.Placement!.TopElevationMm, 3);
    }

    [Fact]
    public void Case32_a_solid_wall_that_covers_only_part_of_the_band_supplies_nothing()
    {
        var junction = Assert.Single(WallJunctions(SplitFacade(4100, 5000, new[] { Host(4550) },
            facadeBottomMm: 450, facadeTopMm: 3000, curtainBaseMm: 450)));

        Assert.Equal(0.0, junction.ContinuousFireRatedLengthMm!.Value);
        Assert.Empty(junction.FacadeWallUniqueIds);
    }

    [Fact]
    public void Case33_a_compartment_wall_that_only_meets_the_slab_edge_has_no_band_and_no_junction()
    {
        // 區劃牆 0–450 只碰到樓板邊緣那一段外牆面，帷幕牆自 450 起算：交集高度為 0，這裡沒有帷幕
        // 外牆面可判，該列不產出。
        var host = new CompartmentWallObservation("W1", new Point2D(4550, 3000), new Point2D(4550, -50),
            0, 450, CurtainWallJunctionReferences.Article79, 60);

        var junctions = Resolve(SplitFacade(4100, 5000, new[] { host }, facadeBottomMm: 450, curtainBaseMm: 450));

        Assert.DoesNotContain(junctions, j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall);
    }

    // --- 外側法線定向（docs §4.6、§10 案例 34–39、決議 15）----------------------------------------

    [Fact]
    public void Case34_a_facade_whose_normal_points_inwards_is_reviewed_exactly_as_one_that_points_out()
    {
        // Revit 的 wall.Orientation 是定位線繞 Z 轉 −90°，與 Wall.Flipped 無關，所以同一道立面上
        // 反向畫的那一片回報的法線朝室內。定向之前，它整片探不到區劃、CW-H 與 CW-O 一起靜默消失。
        var asRead = Assert.Single(WallJunctions(SplitFacade(4100, 5000, new[] { Host(4550) }, normalY: 1)));
        var baseline = Assert.Single(WallJunctions(SplitFacade(4100, 5000, new[] { Host(4550) })));

        Assert.Equal(baseline.JunctionId, asRead.JunctionId);
        Assert.Equal(baseline.CurtainWallUniqueId, asRead.CurtainWallUniqueId);
        Assert.Equal(baseline.ZoneId, asRead.ZoneId);
        Assert.Equal(baseline.ContinuousFireRatedLengthMm!.Value, asRead.ContinuousFireRatedLengthMm!.Value, 3);
        Assert.Equal(baseline.ProjectionDepthMm!.Value, asRead.ProjectionDepthMm!.Value, 3);
        Assert.Equal(baseline.FacadeWallUniqueIds, asRead.FacadeWallUniqueIds);
        Assert.Equal(baseline.Placement!.StartMm.X, asRead.Placement!.StartMm.X, 3);
        Assert.Equal(baseline.Placement!.StartMm.Y, asRead.Placement!.StartMm.Y, 3);
        Assert.Null(asRead.Doubt);
    }

    [Fact]
    public void Case35_a_projection_is_measured_outwards_even_when_the_normal_was_read_inwards()
    {
        // 只修 ZoneOf（找不到區劃就往反向再探）會讓這一列現身，但突出量仍量在反側：600 mm 的突出
        // 算成 −600，被 Math.Max(0, …) 夾成 0，本文的 projectionDepth >= 500 判成「不突出」。
        var junction = Assert.Single(WallJunctions(SplitFacade(4100, 5000,
            new[] { Host(4550, beyondLineMm: OffsetMm + 600) }, normalY: 1)));

        Assert.Equal(600, junction.ProjectionDepthMm!.Value, 3);
    }

    [Fact]
    public void Case36_a_curtain_wall_with_a_compartment_on_both_sides_keeps_the_normal_it_was_read_with()
    {
        // 室內帷幕牆：兩側都探得到區劃，取反是擲硬幣。多數決必須是「嚴格多於」——改成「大於等於」
        // 會把這片讀對的牆翻過去，600 mm 的突出當場變成 0。
        var southZone = new CurtainWallZoneObservation(
            Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000f"), "B 區劃",
            new[] { Rectangle(-2000, -20000, WallLengthMm + 2000, -1) });

        var set = new CurtainWallObservationSet(Package, "LVL", "1F", 0,
            new[] { Zone(), southZone },
            new[] { Wall(Glazing()) },
            new[] { Host(5000, beyondLineMm: OffsetMm + 600) },
            levelElevationsMm: new[] { 0.0, StoreyMm });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(600, junction.ProjectionDepthMm!.Value, 3);
    }

    [Fact]
    public void Case37_a_curtain_wall_with_no_compartment_on_either_side_is_still_left_out_of_the_review()
    {
        // 兩側都探不到代表這片牆不屬於任何區劃。定向不替它補一個，這是既有行為（§9）。
        var set = new CurtainWallObservationSet(Package, "LVL", "1F", 0,
            new[] { new CurtainWallZoneObservation(ZoneId, "A 區劃", new[] { Rectangle(20000, 1, 30000, 20000) }) },
            new[] { Wall(Glazing()) },
            new[] { Host(5000) },
            levelElevationsMm: new[] { 0.0, StoreyMm });

        Assert.Empty(Resolve(set));
    }

    [Fact]
    public void Case38_the_side_that_finds_a_compartment_at_more_stations_wins()
    {
        // 立面跨兩個區劃、中段沒建區劃：中點那一站兩側皆無。單取中點會判不出方向，「任一站」則會
        // 被立面兩端各自貼到的不同區劃拉走——三站多數決 2 > 0，取反。
        var west = new CurtainWallZoneObservation(ZoneId, "A 區劃", new[] { Rectangle(-2000, 1, 4500, 20000) });
        var east = new CurtainWallZoneObservation(
            Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000f"), "B 區劃",
            new[] { Rectangle(7500, 1, WallLengthMm + 2000, 20000) });

        var set = new CurtainWallObservationSet(Package, "LVL", "1F", 0,
            new[] { west, east },
            new[] { new CurtainWallObservation("CW1", new Point2D(0, 0), new Point2D(WallLengthMm, 0),
                new Point2D(0, 1), OffsetMm, 0, StoreyMm, Glazing(), typeName: "帷幕牆 1") },
            new[] { Host(3000, beyondLineMm: OffsetMm + 600) },
            levelElevationsMm: new[] { 0.0, StoreyMm });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.WallToCurtainWall);

        Assert.Equal(ZoneId, junction.ZoneId);
        Assert.Equal(600, junction.ProjectionDepthMm!.Value, 3);
    }

    [Fact]
    public void Case39_reversing_the_normal_moves_nothing_else()
    {
        // 守門測試：定向只改法線正負。動到 Start／End／Direction／ExteriorOffsetMm 就會改變
        // ParameterOf／PointAt、擁有權比對與每一個 junction.id——同一個交接處換了圖號。
        var wall = new CurtainWallObservation("CW1", new Point2D(100, 200), new Point2D(100, 3200),
            new Point2D(-1, 0), OffsetMm, 450, StoreyMm,
            new[] { Panel("P", 0, 3000, 60) },
            new[] { Grid("G", CurtainGridLineDirection.Vertical, 1500) },
            null, "帷幕牆 1");

        var flipped = wall.WithReversedExteriorNormal();

        Assert.Equal(wall.Start.X, flipped.Start.X, 9);
        Assert.Equal(wall.Start.Y, flipped.Start.Y, 9);
        Assert.Equal(wall.End.X, flipped.End.X, 9);
        Assert.Equal(wall.End.Y, flipped.End.Y, 9);
        Assert.Equal(wall.Direction.X, flipped.Direction.X, 9);
        Assert.Equal(wall.Direction.Y, flipped.Direction.Y, 9);
        Assert.Equal(wall.ExteriorOffsetMm, flipped.ExteriorOffsetMm, 9);
        Assert.Equal(wall.LengthMm, flipped.LengthMm, 9);
        Assert.Equal(wall.BaseElevationMm, flipped.BaseElevationMm, 9);
        Assert.Equal(wall.TopElevationMm, flipped.TopElevationMm, 9);
        Assert.Equal(wall.UniqueId, flipped.UniqueId);
        Assert.Equal(wall.TypeName, flipped.TypeName);
        Assert.Equal(wall.NonPlanarReason, flipped.NonPlanarReason);
        Assert.Equal(wall.Panels.Select(p => p.UniqueId), flipped.Panels.Select(p => p.UniqueId));
        Assert.Equal(wall.GridLines.Select(g => g.UniqueId), flipped.GridLines.Select(g => g.UniqueId));

        Assert.Equal(-wall.ExteriorNormal.X, flipped.ExteriorNormal.X, 9);
        Assert.Equal(-wall.ExteriorNormal.Y, flipped.ExteriorNormal.Y, 9);
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

    // --- CW-O 的兩路作答（決議 16、§10 案例 40–44）-----------------------------------------------

    /// <summary>案例 40：一片帷幕牆同時有實心與玻璃嵌板 → CW-O 兩列，各自只帶自己那一路的讀值。</summary>
    [Fact]
    public void Case40_solid_and_glazed_other_panels_are_two_rows()
    {
        var set = Set(Wall(new[]
        {
            Panel("P-solid", 0, 5000, 60),
            Glass("P-glass", 5000, WallLengthMm, ProvidedFireProtection.Yes("是"))
        }));

        var others = Resolve(set).Where(j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther).ToList();

        Assert.Equal(2, others.Count);

        var solid = Assert.Single(others, j => j.PanelAnswerKind == CurtainPanelKinds.SolidRuleText);
        Assert.EndsWith(":solid", solid.JunctionId, StringComparison.Ordinal);
        Assert.Equal(new[] { "P-solid" }, solid.PanelUniqueIds);
        Assert.Equal(60, solid.MinFireRating!.Minutes!.Value);
        Assert.Null(solid.MinFireProtection);

        var glazed = Assert.Single(others, j => j.PanelAnswerKind == CurtainPanelKinds.GlazedRuleText);
        Assert.EndsWith(":glazed", glazed.JunctionId, StringComparison.Ordinal);
        Assert.Equal(new[] { "P-glass" }, glazed.PanelUniqueIds);
        Assert.Equal(ProvidedFireProtectionKind.Yes, glazed.MinFireProtection!.Kind);
        Assert.Null(glazed.MinFireRating);
    }

    /// <summary>案例 41：玻璃嵌板已綁定未勾選 → 讀到「否」，那是未符合而不是資料不足。</summary>
    [Fact]
    public void Case41_an_unticked_glazed_panel_reads_as_no()
    {
        var set = Set(Wall(new[] { Glass("P-glass", 0, WallLengthMm, ProvidedFireProtection.No("否")) }));

        var junction = Single(Resolve(set), CurtainWallJunctionKind.CurtainPanelOther);

        Assert.Equal(ProvidedFireProtectionKind.No, junction.MinFireProtection!.Kind);
    }

    /// <summary>
    /// 案例 42：一片沒填、一片填否 → 取沒填的那一片。與時效那一路同一套理由：先講補得起來的缺口。
    /// </summary>
    [Fact]
    public void Case42_a_missing_protection_outranks_a_declared_no()
    {
        var set = Set(Wall(new[]
        {
            Glass("P-no", 0, 5000, ProvidedFireProtection.No("否")),
            Glass("P-missing", 5000, WallLengthMm, ProvidedFireProtection.Missing("型別未提供防火保護"))
        }));

        var junction = Single(Resolve(set), CurtainWallJunctionKind.CurtainPanelOther);

        Assert.Equal(ProvidedFireProtectionKind.Missing, junction.MinFireProtection!.Kind);
    }

    /// <summary>
    /// 案例 43：沒宣告種類的嵌板自成一列，<c>panelKind</c> 不供值——判定層據此讓引擎答資料不足。
    /// </summary>
    [Fact]
    public void Case43_panels_without_a_declared_kind_are_their_own_row()
    {
        var set = Set(Wall(new[]
        {
            Panel("P-solid", 0, 5000, 60),
            Panel("P-unknown", 5000, WallLengthMm, 60, kind: null)
        }));

        var others = Resolve(set).Where(j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther).ToList();

        Assert.Equal(2, others.Count);
        var undeclared = Assert.Single(others, j => j.PanelAnswerKind is null);
        Assert.EndsWith(":undeclared", undeclared.JunctionId, StringComparison.Ordinal);
        Assert.Equal(new[] { "P-unknown" }, undeclared.PanelUniqueIds);
        Assert.Null(undeclared.MinFireRating);
        Assert.Null(undeclared.MinFireProtection);
    }

    /// <summary>
    /// 案例 44：帷幕牆門窗與玻璃同一路（決議 16）。這順手修掉一個舊缺陷——窗本來就不帶時效，過去
    /// 它會被算進時效那一路的取小，一扇窗就把整片牆的第79條之4 拖成資料不足。
    /// </summary>
    [Fact]
    public void Case44_a_curtain_wall_window_answers_with_its_protection_not_a_rating()
    {
        var set = Set(Wall(new[]
        {
            Panel("P-solid", 0, 5000, 60),
            Panel("P-window", 5000, WallLengthMm, null, isOpening: true, protection: ProvidedFireProtection.Yes("是"))
        }));

        var others = Resolve(set).Where(j => j.Kind == CurtainWallJunctionKind.CurtainPanelOther).ToList();

        var solid = Assert.Single(others, j => j.PanelAnswerKind == CurtainPanelKinds.SolidRuleText);
        Assert.Equal(60, solid.MinFireRating!.Minutes!.Value);
        Assert.DoesNotContain("P-window", solid.PanelUniqueIds);

        var glazed = Assert.Single(others, j => j.PanelAnswerKind == CurtainPanelKinds.GlazedRuleText);
        Assert.Equal(new[] { "P-window" }, glazed.PanelUniqueIds);
        Assert.Equal(ProvidedFireProtectionKind.Yes, glazed.MinFireProtection!.Kind);
    }

    /// <summary>
    /// 案例 47（守門）：玻璃嵌板勾了防火保護也不供給 CW-V 的 900 mm 但書高度。第79條之3 但書要的是
    /// 「同等以上防火時效」，防火設備不是防火時效（§9、決議 13 與 16）。高度供給 0 而不是不供值：
    /// 宣告為玻璃的嵌板沒有時效是已知，不是缺漏，所以樓板不突出時是未符合，不是資料不足。
    /// </summary>
    [Fact]
    public void Case47_a_protected_glazed_panel_does_not_supply_the_vertical_but_clause()
    {
        var panels = new[] { Glass("P-glass", 0, WallLengthMm, ProvidedFireProtection.Yes("是")) };
        var floor = new CompartmentFloorObservation("F1",
            new[] { Rectangle(-500, -50, WallLengthMm + 500, 8000) }, StoreyMm, 60);
        var set = new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() }, new[] { Wall(panels) }, compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedHeightMm);
        Assert.Null(junction.MinFireRating);
        Assert.Null(junction.MinFireProtection);
    }

    [Fact]
    public void A_glazed_panel_with_a_rating_filled_in_still_does_not_count_towards_the_band()
    {
        // 決議 16：玻璃的時效讀值不進判定。填了 60 也不會讓玻璃供給層間帶的高度（案例 48 的 CW-V 面）。
        var panels = new[]
        {
            new CurtainPanelObservation("P-glass", 0, WallLengthMm, 0, StoreyMm * 2,
                ProvidedFireRating.Rated(60, "60"), false, ProvidedFireProtection.Yes("是"), kind: CurtainPanelKind.Glazed)
        };

        var junction = Single(Resolve(SpandrelSet(panels)), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedHeightMm);
    }

    [Fact]
    public void A_glazed_neighbour_is_a_real_break_rather_than_a_gap_in_the_data()
    {
        // 樓板下 450 mm 的 60 min 實板，上方緊接玻璃：玻璃那一側是真實斷點，依實測的 450 mm 判定。
        var set = SpandrelSet(
            new[]
            {
                Panel("S-low", 0, WallLengthMm, 60, bottom: StoreyMm - 450, top: StoreyMm),
                Panel("S-zero", 0, WallLengthMm, 0, bottom: 0, top: StoreyMm - 450),
                Glass("G-high", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), bottom: StoreyMm, top: StoreyMm * 2)
            },
            gridLines: new[] { Grid("G-1", CurtainGridLineDirection.Horizontal, StoreyMm) });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.False(junction.IsDoubtful);
        Assert.Equal(450, junction.ContinuousFireRatedHeightMm!.Value, 3);
        Assert.Contains("G-high", junction.PanelUniqueIds);
    }

    [Fact]
    public void A_window_ending_at_the_slab_lets_the_spandrel_above_be_measured_from_the_slab()
    {
        // 最常見的配置：樓板以下是視窗玻璃，水平 grid line 正好在樓板高程，以上是 1200 mm 的 60 min
        // 實心層間板。層間帶從實心那一側起算，不是因為下方先碰到玻璃就報 0。
        var set = SpandrelSet(
            new[]
            {
                Glass("G-window", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), bottom: 0, top: StoreyMm),
                Panel("S-spandrel", 0, WallLengthMm, 60, bottom: StoreyMm, top: StoreyMm + 1200),
                Glass("G-above", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), bottom: StoreyMm + 1200, top: StoreyMm * 2)
            },
            gridLines: new[]
            {
                Grid("G-1", CurtainGridLineDirection.Horizontal, StoreyMm),
                Grid("G-2", CurtainGridLineDirection.Horizontal, StoreyMm + 1200)
            });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.False(junction.IsDoubtful);
        Assert.Equal(1200, junction.ContinuousFireRatedHeightMm!.Value, 3);
        Assert.Equal(60, junction.MinFireRating!.Minutes!.Value);
    }

    [Fact]
    public void Two_glazed_panels_meeting_at_the_slab_still_measure_zero()
    {
        var set = SpandrelSet(
            new[]
            {
                Glass("G-low", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), bottom: 0, top: StoreyMm),
                Glass("G-high", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), bottom: StoreyMm, top: StoreyMm * 2)
            },
            gridLines: new[] { Grid("G-1", CurtainGridLineDirection.Horizontal, StoreyMm) });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(0.0, junction.ContinuousFireRatedHeightMm);
    }

    [Fact]
    public void A_slab_stopping_just_behind_the_curtain_wall_still_meets_it()
    {
        // 實機模型（帷幕牆 290097 × 樓板 290839）：樓板邊緣停在帷幕牆定位線內側 75 mm，中間是防火填塞的縫。
        // 那仍是這一層的層間交接，突出量為 0，全玻璃的層間帶高度為 0 — 之前這裡一個交接都不產生，
        // 嵌板全數落到 CW-O，以防火保護過關。
        var set = SetBackSlab(setBackMm: 75);

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal("F1", junction.HostUniqueId);
        Assert.Equal(0.0, junction.ProjectionDepthMm);
        Assert.Equal(0.0, junction.ContinuousFireRatedHeightMm);
        Assert.Contains("P-glass", junction.PanelUniqueIds);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    [InlineData(100, false)]
    public void The_search_tolerance_is_where_a_set_back_slab_stops_meeting_the_curtain_wall(double pastToleranceMm, bool meets)
    {
        var set = SetBackSlab(setBackMm: CurtainWallJunctionOptions.Default.JunctionSearchToleranceMm + pastToleranceMm);

        Assert.Equal(meets, Resolve(set).Any(j => j.Kind == CurtainWallJunctionKind.FloorToCurtainWall));
    }

    [Fact]
    public void A_set_back_slab_is_found_behind_the_facade_even_when_the_normal_was_read_inwards()
    {
        // Revit 的 wall.Orientation 可能朝室內（決議 15）：退縮線要在定向之後才往區劃側退，否則會退到室外。
        var floor = new CompartmentFloorObservation("F1",
            new[] { Rectangle(-500, 75, WallLengthMm + 500, 8000) }, StoreyMm, 60);
        var wall = new CurtainWallObservation("CW1", new Point2D(0, 0), new Point2D(WallLengthMm, 0), new Point2D(0, 1),
            OffsetMm, 0, StoreyMm * 2,
            new[] { Glass("P-glass", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), top: StoreyMm * 2) },
            typeName: "帷幕牆 1");
        var set = new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() }, new[] { wall }, compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2 });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        Assert.Equal(0.0, junction.ProjectionDepthMm);
        Assert.Equal(0.0, junction.ContinuousFireRatedHeightMm);
    }

    [Fact]
    public void A_slab_edge_running_away_from_the_curtain_wall_meets_it_only_where_it_is_within_tolerance()
    {
        // 樓板邊緣斜向退縮：x = 0 處貼齊定位線，x = 12000 處退 650 mm。超過 300 mm 的那一段不是交接。
        var floor = new CompartmentFloorObservation("F1",
            new[]
            {
                new[] { new Point2D(-500, 0), new Point2D(WallLengthMm, 650), new Point2D(WallLengthMm, 8000), new Point2D(-500, 8000) }
            }, StoreyMm, 60);
        var set = new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() },
            new[] { Wall(new[] { Glass("P-glass", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), top: StoreyMm * 2) }, top: StoreyMm * 2) },
            compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2 });

        var junction = Single(Resolve(set), CurtainWallJunctionKind.FloorToCurtainWall);

        // 邊緣 y = 650 × (x + 500) / 12500，退 300 mm 處在 x = 5269.2。
        var placement = junction.Placement!;
        Assert.Equal(0.0, Math.Min(placement.StartMm.X, placement.EndMm.X), 3);
        Assert.Equal(5269.2, Math.Max(placement.StartMm.X, placement.EndMm.X), 1);
    }

    [Fact]
    public void A_set_back_slab_on_the_storey_above_keeps_a_two_storey_curtain_wall_out_of_article_79_2()
    {
        // 上一層的樓板同樣退在帷幕牆內側 75 mm：它照樣與帷幕牆交接，不是連跨複數樓層的垂直空間。
        var above = new CompartmentFloorObservation("F2",
            new[] { Rectangle(-500, 75, WallLengthMm + 500, 8000) }, StoreyMm, 60);
        var set = new CurtainWallObservationSet(Package, "LVL", "1F", 0,
            new[] { Zone() },
            new[] { Wall(Glazing(top: StoreyMm * 2), top: StoreyMm * 2) },
            compartmentFloors: new[] { above },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2 });

        Assert.DoesNotContain(Resolve(set), j => j.Doubt?.Kind == CurtainWallJunctionDoubtKind.VerticalCompartmentSpace);
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

    private static IReadOnlyList<CurtainWallJunction> WallJunctions(CurtainWallObservationSet set) =>
        Resolve(set).Where(j => j.Kind == CurtainWallJunctionKind.WallToCurtainWall).ToList();

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
        ProvidedFireProtection? protection = null,
        CurtainPanelKind? kind = CurtainPanelKind.Solid) =>
        new(uniqueId, startMm, endMm, bottom, top,
            minutes is double m ? ProvidedFireRating.Rated(m, m.ToString("0")) : ProvidedFireRating.Missing("參數值為空白"),
            isOpening, protection, kind: kind);

    /// <summary>
    /// 一片宣告為玻璃的嵌板（決議 16）：它答的是 <c>防火檢討_設計防火保護</c>，時效讀值刻意留空白，
    /// 因為玻璃填不出構造時效——這正是分兩路的理由。
    /// </summary>
    private static CurtainPanelObservation Glass(
        string uniqueId,
        double startMm,
        double endMm,
        ProvidedFireProtection protection,
        double bottom = 0,
        double top = StoreyMm) =>
        new(uniqueId, startMm, endMm, bottom, top, ProvidedFireRating.Missing("玻璃嵌板不填構造時效"),
            false, protection, kind: CurtainPanelKind.Glazed);

    /// <summary>A panel whose rating is whatever the adapter made of the parameter — absent, blank or a value.</summary>
    private static CurtainPanelObservation Unrated(
        string uniqueId, double startMm, double endMm, double bottom, double top, ProvidedFireRating rating) =>
        new(uniqueId, startMm, endMm, bottom, top, rating, false, null, kind: CurtainPanelKind.Solid);

    private static CurtainGridLineObservation Grid(string uniqueId, CurtainGridLineDirection direction, double positionMm) =>
        new(uniqueId, direction, positionMm);

    private static IReadOnlyList<CurtainPanelObservation> Glazing(double top = StoreyMm) =>
        new[] { Panel("P-glass", 0, WallLengthMm, 0, top: top) };

    /// <summary>
    /// 立面上 <paramref name="startMm"/>–<paramref name="endMm"/> 這一段不鋪嵌板，其餘為不具時效的
    /// 玻璃：§4.2「建模要求」講的就是這個樣子——交接帶那一柱留空，改以實體外牆表達。
    /// </summary>
    private static IReadOnlyList<CurtainPanelObservation> GlazingExcept(
        double startMm, double endMm, double bottom = 0, double top = StoreyMm) =>
        new[]
        {
            Panel("P-glass-left", 0, startMm, 0, bottom: bottom, top: top),
            Panel("P-glass-right", endMm, WallLengthMm, 0, bottom: bottom, top: top)
        };

    /// <summary>
    /// 一道躺在帷幕牆定位面內（y = 0，沿 x 走）的實體外牆：決議 13 起 CW-H 的但書長度由它供給。
    /// </summary>
    private static FacadeWallObservation Facade(
        double startMm,
        double endMm,
        double? minutes = 60,
        double bottom = 0,
        double top = StoreyMm,
        string uniqueId = "W-facade") =>
        new(uniqueId, new Point2D(startMm, 0), new Point2D(endMm, 0), bottom, top, "RC 牆 15cm",
            minutes is double m ? ProvidedFireRating.Rated(m, m.ToString("0")) : ProvidedFireRating.Missing("參數值為空白"));

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
        IEnumerable<CompartmentFloorObservation>? floors = null,
        IEnumerable<FacadeWallObservation>? facades = null) =>
        new(Package, "LVL", "1F", 0, new[] { Zone() }, new[] { wall }, hosts, floors, facades,
            new[] { 0.0, StoreyMm });

    /// <summary>
    /// §4.2「建模要求」的立面：帷幕牆 ─ 實體外牆 ─ 帷幕牆。左片自 <paramref name="gapStartMm"/> 往
    /// x = 0 畫、右片自 <paramref name="gapEndMm"/> 往 x = <see cref="WallLengthMm"/> 畫——兩片的軸向
    /// 相反，與實機模型相同，交點在兩片的定位線上都是負的參數，所以歸屬不能用「沿軸較低側」。
    /// 外側法線由 <paramref name="normalY"/> 給定：`-1` 是朝室外（讀對了），`+1` 是朝室內——
    /// Revit 的 `wall.Orientation` 兩種都給得出來，由解析層自己定向（§4.6、決議 15）。
    /// </summary>
    private static CurtainWallObservationSet SplitFacade(
        double gapStartMm,
        double gapEndMm,
        IEnumerable<CompartmentWallObservation> hosts,
        double? minutes = 60,
        double facadeBottomMm = 0,
        double facadeTopMm = StoreyMm,
        double curtainBaseMm = 0,
        double curtainTopMm = StoreyMm,
        double seamMm = 0,
        string leftUniqueId = "CW-left",
        string rightUniqueId = "CW-right",
        double normalY = -1)
    {
        var left = new CurtainWallObservation(leftUniqueId, new Point2D(gapStartMm, 0), new Point2D(0, 0),
            new Point2D(0, normalY), OffsetMm, curtainBaseMm, curtainTopMm,
            new[] { Panel("P-left", 0, gapStartMm, 0, bottom: curtainBaseMm, top: curtainTopMm) },
            typeName: "帷幕牆 1");

        var right = new CurtainWallObservation(rightUniqueId, new Point2D(gapEndMm, 0), new Point2D(WallLengthMm, 0),
            new Point2D(0, normalY), OffsetMm, curtainBaseMm, curtainTopMm,
            new[] { Panel("P-right", 0, WallLengthMm - gapEndMm, 0, bottom: curtainBaseMm, top: curtainTopMm) },
            typeName: "帷幕牆 2");

        return new CurtainWallObservationSet(Package, "LVL", "1F", 0,
            new[] { Zone() },
            new[] { left, right },
            hosts,
            facadeWalls: new[] { Facade(gapStartMm + seamMm, gapEndMm - seamMm, minutes, facadeBottomMm, facadeTopMm) },
            levelElevationsMm: new[] { 0.0, StoreyMm });
    }

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

    /// <summary>
    /// 一層全玻璃（宣告為玻璃、勾了防火保護）的帷幕牆跨過 3600 的樓板，樓板邊緣退在定位線內側
    /// <paramref name="setBackMm"/>（y 向北為室內）。
    /// </summary>
    private static CurtainWallObservationSet SetBackSlab(double setBackMm)
    {
        var floor = new CompartmentFloorObservation("F1",
            new[] { Rectangle(-500, setBackMm, WallLengthMm + 500, 8000) }, StoreyMm, 60);

        return new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() },
            new[] { Wall(new[] { Glass("P-glass", 0, WallLengthMm, ProvidedFireProtection.Yes("是"), top: StoreyMm * 2) }, top: StoreyMm * 2) },
            compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2 });
    }

    private static CurtainWallObservationSet SpandrelSet(
        IReadOnlyList<CurtainPanelObservation> panels,
        double? required = 60,
        double slabEdgeMm = 50,
        IEnumerable<CurtainGridLineObservation>? gridLines = null)
    {
        var floor = new CompartmentFloorObservation("F1",
            new[] { Rectangle(-500, -slabEdgeMm, WallLengthMm + 500, 8000) }, StoreyMm, required);

        return new CurtainWallObservationSet(Package, "LVL", "2F", StoreyMm,
            new[] { Zone() },
            new[] { Wall(panels, gridLines, top: StoreyMm * 2) },
            compartmentFloors: new[] { floor },
            levelElevationsMm: new[] { 0.0, StoreyMm, StoreyMm * 2 });
    }
}
