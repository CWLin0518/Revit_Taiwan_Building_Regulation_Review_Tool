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
            FireReviewZoneParameters.Use | FireReviewZoneParameters.Sprinklered | FireReviewZoneParameters.FloorNumber) =>
        new(uid, name, number: "1", levelName: "FL9", areaSchemeName: "防火區劃",
            areaSquareMeters: areaM2, use: use, sprinklered: sprinklered, floorNumber: floorNumber, present: present);

    [Fact]
    public void A_zone_row_reports_the_area_revit_measured_and_offers_no_way_to_change_it()
    {
        var zone = Zone(areaM2: 71.59);

        Assert.Equal("71.59 m²", zone.AreaText);
        Assert.Equal(71.59, zone.AreaSquareMeters);
        // The row's only writable facts are the three zone parameters; there is no area setter.
        Assert.DoesNotContain(typeof(FireReviewZoneRow).GetProperties(), p => p.Name == "AreaSquareMeters" && p.CanWrite);
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

        Assert.Equal(new[] { ReviewInputSources.Sprinklered, ReviewInputSources.FloorNumber }, zone.MissingParameters);
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
    public void A_zone_row_rejects_a_blank_id_and_a_negative_area()
    {
        Assert.Throws<ArgumentException>(() => Zone(uid: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Zone(areaM2: -1));
    }

    [Fact]
    public void The_project_row_names_the_parameters_the_document_does_not_carry()
    {
        var project = new FireReviewProjectRow("info", present: FireReviewProjectParameters.FireResistive);

        Assert.Equal(new[] { ReviewInputSources.BuildingUse, ReviewInputSources.FloorsAboveGround },
            project.MissingParameters);
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
