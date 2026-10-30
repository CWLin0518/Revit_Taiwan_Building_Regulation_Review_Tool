using System;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Parameters;
using BuildingRegulationReview.Application.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

/// <summary>
/// The 區劃 and Project Information rows the batch panel edits. These are instance parameters, so a
/// zone row reaches one Area and the project row reaches one element — unlike the Type rows, whose
/// reach is the whole project.
/// </summary>
public sealed class FireReviewInputRowTests
{
    private static FireReviewZoneRow Zone(
        string uid = "area-1",
        string name = "區劃 1",
        string? use = null,
        bool? sprinklered = null,
        int? floorNumber = null,
        double? areaM2 = 71.59,
        FireReviewZoneParameters present =
            FireReviewZoneParameters.Use | FireReviewZoneParameters.Sprinklered |
            FireReviewZoneParameters.FloorNumber | FireReviewZoneParameters.UseGroup,
        bool? linksRefugeFloor = null,
        bool? cannotBeSubdivided = null) =>
        new(uid, name, number: "1", levelName: "FL9", areaSchemeName: "防火區劃",
            areaSquareMeters: areaM2, use: use, sprinklered: sprinklered, floorNumber: floorNumber, present: present,
            linksRefugeFloor: linksRefugeFloor,
            cannotBeSubdivided: cannotBeSubdivided);

    [Fact]
    public void A_zone_row_reports_the_area_revit_measured_and_offers_no_way_to_change_it()
    {
        var zone = Zone(areaM2: 71.59);

        Assert.Equal("71.59 m²", zone.AreaText);
        Assert.Equal(71.59, zone.AreaSquareMeters);
        // The row's only writable facts are the three zone parameters; there is no area setter.
        Assert.DoesNotContain(typeof(FireReviewZoneRow).GetProperties(), p => p.Name == "AreaSquareMeters" && p.CanWrite);
    }

    /// <summary>The 區劃 an Area belongs to is carried as read; blank means the Area stands alone.</summary>
    [Fact]
    public void A_zone_row_carries_which_zone_it_belongs_to()
    {
        Assert.Equal("pkg:zone", new FireReviewZoneRow("area-1", "區劃 1", zoneKey: " pkg:zone ").ZoneKey);
        Assert.Null(new FireReviewZoneRow("area-1", "區劃 1", zoneKey: " ").ZoneKey);
    }

    [Fact]
    public void An_unplaced_zone_shows_no_area_rather_than_zero()
    {
        Assert.Equal("—", Zone(areaM2: null).AreaText);
    }

    [Fact]
    public void A_zone_row_names_the_parameters_its_area_does_not_carry()
    {
        var zone = Zone(present: FireReviewZoneParameters.Use);

        Assert.Equal(
            new[] { ReviewInputSources.Sprinklered, ReviewInputSources.FloorNumber, ReviewInputSources.UseGroup },
            zone.MissingParameters);
        Assert.Empty(Zone().MissingParameters);
    }

    [Fact]
    public void A_zone_row_keeps_sprinklers_as_a_tri_state_so_unset_is_not_no()
    {
        Assert.Null(Zone(sprinklered: null).Sprinklered);
        Assert.False(Zone(sprinklered: false).Sprinklered);
        Assert.True(Zone(sprinklered: true).Sprinklered);
    }

    [Fact]
    public void A_zone_row_keeps_the_refuge_floor_link_as_a_tri_state()
    {
        Assert.Null(Zone(linksRefugeFloor: null).LinksRefugeFloor);
        Assert.False(Zone(linksRefugeFloor: false).LinksRefugeFloor);
        Assert.True(Zone(linksRefugeFloor: true).LinksRefugeFloor);
    }

    /// <summary>
    /// 決議 27: 避難層通達 is not a required parameter. Naming it on every Area would turn the 提醒
    /// column red across a whole model over something only a 挑空 needs, so it is reported missing on
    /// a 挑空 alone — and the three real ones are reported whatever the use. 連跨樓層數 and 連通區劃面積
    /// are traced, never typed (決議 35), so no Area is ever asked for them.
    /// </summary>
    [Fact]
    public void The_third_paragraphs_parameters_are_only_missed_on_an_atrium()
    {
        var everything = FireReviewZoneParameters.Use | FireReviewZoneParameters.Sprinklered |
                         FireReviewZoneParameters.FloorNumber | FireReviewZoneParameters.UseGroup;

        Assert.Empty(Zone(use: "辦公", present: everything).MissingParameters);
        Assert.False(Zone(use: "辦公", present: everything).IsAtrium);

        var atrium = Zone(use: ZoneUses.Atrium, present: everything);
        Assert.True(atrium.IsAtrium);
        Assert.Equal(new[] { ReviewInputSources.LinksRefugeFloor }, atrium.MissingParameters);

        Assert.Empty(Zone(use: ZoneUses.Atrium,
            present: everything | FireReviewZoneParameters.LinksRefugeFloor).MissingParameters);

        // A 樓梯間 is a 垂直區劃 too, but 第3項 is written for 挑空 alone.
        Assert.Empty(Zone(use: ZoneUses.Stairwell, present: everything).MissingParameters);
    }

    [Fact]
    public void A_zone_row_keeps_the_subdivision_declaration_as_a_tri_state()
    {
        Assert.Null(Zone(cannotBeSubdivided: null).CannotBeSubdivided);
        Assert.False(Zone(cannotBeSubdivided: false).CannotBeSubdivided);
        Assert.True(Zone(cannotBeSubdivided: true).CannotBeSubdivided);
    }

    /// <summary>
    /// 第79條之1 決議 9: 防火檢討_無法區劃分隔 is not a required parameter either, so it is reported
    /// missing on the six uses that article names and on nothing else — same shape, same reason as
    /// the 第3項 pair above.
    /// </summary>
    [Theory]
    [InlineData(ZoneUses.Auditorium)]
    [InlineData(ZoneUses.ProductionLine)]
    [InlineData(ZoneUses.Classroom)]
    [InlineData(ZoneUses.Gymnasium)]
    [InlineData(ZoneUses.RetailMarket)]
    [InlineData(ZoneUses.CarPark)]
    public void The_subdivision_parameter_is_only_missed_on_an_article_79_1_use(string use)
    {
        var everything = FireReviewZoneParameters.Use | FireReviewZoneParameters.Sprinklered |
                         FireReviewZoneParameters.FloorNumber | FireReviewZoneParameters.UseGroup;

        var zone = Zone(use: use, present: everything);
        Assert.True(zone.IsArticle79_1Use);
        Assert.Equal(new[] { ReviewInputSources.CannotBeSubdivided }, zone.MissingParameters);

        Assert.Empty(Zone(use: use, present: everything | FireReviewZoneParameters.CannotBeSubdivided)
            .MissingParameters);
    }

    [Theory]
    [InlineData("辦公")]
    [InlineData("觀眾廳")]
    [InlineData(ZoneUses.Atrium)]
    [InlineData(null)]
    public void A_use_outside_the_article_is_never_asked_for_a_subdivision_declaration(string? use)
    {
        var everything = FireReviewZoneParameters.Use | FireReviewZoneParameters.Sprinklered |
                         FireReviewZoneParameters.FloorNumber | FireReviewZoneParameters.UseGroup |
                         FireReviewZoneParameters.LinksRefugeFloor;

        var zone = Zone(use: use, present: everything);
        Assert.False(zone.IsArticle79_1Use);
        Assert.DoesNotContain(ReviewInputSources.CannotBeSubdivided, zone.MissingParameters);
    }

    [Fact]
    public void A_zone_row_rejects_a_blank_id_and_a_negative_area()
    {
        Assert.Throws<ArgumentException>(() => Zone(uid: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Zone(areaM2: -1));
    }

    [Fact]
    public void The_project_row_names_the_parameters_the_document_does_not_carry()
    {
        var project = new FireReviewProjectRow("info", present: FireReviewProjectParameters.FireResistive);

        Assert.Equal(new[] { ReviewInputSources.FloorsAboveGround }, project.MissingParameters);
    }

    /// <summary>建築物高度 is measured from the model, so the panel has nothing to offer for it.</summary>
    [Fact]
    public void The_project_row_has_no_building_height_to_type()
    {
        Assert.DoesNotContain(typeof(FireReviewProjectRow).GetProperties(), p => p.Name.Contains("Height"));
        Assert.DoesNotContain(typeof(FireReviewProjectParameters).GetEnumNames(), n => n.Contains("Height"));
    }

    [Fact]
    public void The_set_orders_zones_by_level_and_drops_duplicates()
    {
        var set = new FireReviewParameterSet(
            new FireReviewTypeTable(null),
            new[]
            {
                new FireReviewZoneRow("z-2", "區劃 2", levelName: "FL9"),
                new FireReviewZoneRow("z-1", "區劃 1", levelName: "FL1"),
                new FireReviewZoneRow("z-1", "區劃 1 重複", levelName: "FL1")
            });

        Assert.Equal(new[] { "z-1", "z-2" }, set.Zones.Select(z => z.ElementUniqueId));
    }

    /// <summary>
    /// The one fact the whole review hangs on: without it every rule's applicability fails and the
    /// results are all 資料不足, so the panel has to be able to say so before anything is run.
    /// </summary>
    [Fact]
    public void The_set_says_whether_fire_resistive_construction_is_set()
    {
        Assert.False(new FireReviewParameterSet(new FireReviewTypeTable(null)).FireResistiveConstructionIsSet);
        Assert.False(Set(fireResistive: null).FireResistiveConstructionIsSet);
        Assert.False(Set(fireResistive: false).FireResistiveConstructionIsSet);
        Assert.True(Set(fireResistive: true).FireResistiveConstructionIsSet);
    }

    [Fact]
    public void The_set_lists_the_zones_still_missing_their_sprinkler_answer()
    {
        var set = new FireReviewParameterSet(
            new FireReviewTypeTable(null),
            new[]
            {
                Zone("z-1", sprinklered: true),
                Zone("z-2", sprinklered: null),
                Zone("z-3", sprinklered: false)
            });

        Assert.Equal(new[] { "z-2" }, set.ZonesMissingSprinklers.Select(z => z.ElementUniqueId));
    }

    [Fact]
    public void Area_rows_never_request_the_model_derived_finish_grade()
    {
        var set = new FireReviewParameterSet(
            new FireReviewTypeTable(null),
            new[]
            {
                Zone("z-1", floorNumber: 11),
                Zone("z-2", floorNumber: 11),
                Zone("z-3", floorNumber: 10),
                Zone("z-4", floorNumber: null),
                Zone("z-5", floorNumber: 20)
            });

        Assert.All(set.Zones, zone => Assert.DoesNotContain(ReviewInputSources.InteriorFinish, zone.MissingParameters));
    }

    /// <summary>The panel writes every tab in one go, so the edits share one element-addressed shape.</summary>
    [Fact]
    public void Zone_and_project_edits_address_their_own_element()
    {
        var zone = FireReviewParameterEdit.OfText("area-1", ReviewInputSources.Sprinklered, "1");
        Assert.Equal("area-1", zone.ElementUniqueId);
        Assert.Equal("1", zone.Text);

        var project = FireReviewParameterEdit.OfText("info", ReviewInputSources.FloorsAboveGround, "10");
        Assert.Equal("info", project.ElementUniqueId);
        Assert.Equal("10", project.Text);
    }

    private static FireReviewParameterSet Set(bool? fireResistive) =>
        new(new FireReviewTypeTable(null), null,
            new FireReviewProjectRow("info", fireResistiveConstruction: fireResistive));
}

/// <summary>
/// C-07：防火檢討_防火構造建築物 的紅字原本只分「是」與「不是是」，於是明確填「否」的人被告知會判
/// 「資料不足」——實際上四條規則的 appliesWhen 都以 fireResistiveConstruction == true 開頭，填否時
/// 條件已知為假，每一列都是「不適用」。
/// </summary>
public sealed class FireResistiveConstructionNoticeTests
{
    [Fact]
    public void Unanswered_still_warns_about_資料不足()
    {
        var notice = FireResistiveConstructionNotice.For(null);

        Assert.Contains("未填", notice);
        Assert.Contains("資料不足", notice);
        Assert.DoesNotContain("不適用", notice);
    }

    /// <summary>
    /// 填否要說「每一列都不適用」，而且必須點明不適用不代表合格：檢討表上的「不適用」讀起來像
    /// 「沒有要檢討的事」，與紅字想傳達的「這份檢討作不了數」正好相反。
    /// </summary>
    [Fact]
    public void Answering_否_warns_that_every_row_will_be_不適用_and_that_this_is_not_a_pass()
    {
        var notice = FireResistiveConstructionNotice.For(false);

        Assert.Contains("不適用", notice);
        Assert.Contains("不代表合格", notice);
        Assert.DoesNotContain("資料不足", notice);
    }

    [Fact]
    public void Answering_是_warns_about_nothing()
    {
        Assert.Equal("", FireResistiveConstructionNotice.For(true));
    }
}
