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
