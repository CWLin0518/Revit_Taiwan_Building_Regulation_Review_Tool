using System;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

public sealed class FireReviewTypeTableTests
{
    private static FireReviewTypeRow Row(
        string uid = "T-1",
        CandidateCategory category = CandidateCategory.Wall,
        double? dimensionCm = 20,
        string? material = "RC",
        double? coverCm = null,
        string? rating = null,
        bool? protection = null,
        int inView = 3,
        int inProject = 9,
        FireReviewTypeParameters present = FireReviewTypeParameters.Rating | FireReviewTypeParameters.Material) =>
        new(uid, category, "RC20", familyName: null, instanceCount: inView, projectInstanceCount: inProject,
            dimensionMeters: dimensionCm is double d ? d / 100 : (double?)null,
            material: material,
            coverMeters: coverCm is double c ? c / 100 : (double?)null,
            providedRating: rating, providedProtection: protection, present: present);

    [Fact]
    public void A_row_derives_the_rating_its_material_and_thickness_give()
    {
        var row = Row(dimensionCm: 8, material: "RC");

        Assert.Equal(60, row.Derivation.Minutes);
        Assert.Equal("60 min", row.RatingEdit()!.Text);
        Assert.Equal(FireRatingParameters.Provided, row.RatingEdit()!.ParameterName);
    }

    [Fact]
    public void A_row_whose_rating_already_matches_would_change_nothing()
    {
        Assert.False(Row(dimensionCm: 8, rating: "60 min").WouldChangeRating);
        Assert.False(Row(dimensionCm: 8, rating: "1hr").WouldChangeRating);
        Assert.True(Row(dimensionCm: 8, rating: "30 min").WouldChangeRating);
        Assert.True(Row(dimensionCm: 8, rating: null).WouldChangeRating);
        Assert.True(Row(dimensionCm: 8, rating: "耐燃").WouldChangeRating);
    }

    [Fact]
    public void A_row_that_derives_nothing_would_change_nothing()
    {
        Assert.False(Row(material: null).WouldChangeRating);
        Assert.False(Row(category: CandidateCategory.StructuralFraming).WouldChangeRating);
        Assert.Null(Row(material: null).RatingEdit());
    }

    /// <summary>The project count is the real reach of a Type edit, so it can never be the smaller number.</summary>
    [Fact]
    public void The_project_count_is_never_less_than_what_the_view_showed()
    {
        Assert.Equal(9, Row(inView: 3, inProject: 9).ProjectInstanceCount);
        Assert.Equal(7, Row(inView: 7, inProject: 2).ProjectInstanceCount);
    }

    [Fact]
    public void The_table_orders_members_before_openings_and_drops_duplicate_types()
    {
        var table = new FireReviewTypeTable(new[]
        {
            Row("T-door", CandidateCategory.Door, dimensionCm: null, material: null),
            Row("T-wall", CandidateCategory.Wall),
            Row("T-wall", CandidateCategory.Wall),
            Row("T-floor", CandidateCategory.Floor)
        });

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(new[] { CandidateCategory.Wall, CandidateCategory.Floor, CandidateCategory.Door },
            table.Rows.Select(r => r.Category));
    }

    [Fact]
    public void The_table_separates_rows_waiting_on_material_from_rows_waiting_on_cover()
    {
        var table = new FireReviewTypeTable(new[]
        {
            Row("T-1", material: null),
            Row("T-2", material: "SC", coverCm: null),
            Row("T-3", material: "SC", coverCm: 4),
            Row("T-4", material: "RC", dimensionCm: 20)
        });

        Assert.Equal(new[] { "T-1" }, table.AwaitingMaterial.Select(r => r.TypeUniqueId));
        Assert.Equal(new[] { "T-2" }, table.AwaitingCover.Select(r => r.TypeUniqueId));
        Assert.Equal(new[] { "T-3", "T-4" }, table.Derivable.Select(r => r.TypeUniqueId).OrderBy(x => x));
    }

    [Fact]
    public void An_edit_keeps_text_and_length_apart()
    {
        var text = FireReviewParameterEdit.OfText("T-1", StructuralMaterialParameters.Material, "RC");
        Assert.False(text.IsLength);
        Assert.Equal("RC", text.Text);
        Assert.Null(text.LengthMeters);

        var length = FireReviewParameterEdit.OfLength("T-1", StructuralMaterialParameters.Cover, 0.04);
        Assert.True(length.IsLength);
        Assert.Equal(0.04, length.LengthMeters);
        Assert.Null(length.Text);
    }

    /// <summary>The panel must never set the parameter the rules write back to (spec 11.5 step 4).</summary>
    [Fact]
    public void The_required_rating_parameter_cannot_be_edited_by_the_panel()
    {
        var thrown = Assert.Throws<ArgumentException>(() =>
            FireReviewParameterEdit.OfText("T-1", FireRatingParameters.Required, "60 min"));

        Assert.Contains(FireRatingParameters.Required, thrown.Message);
    }

    [Fact]
    public void An_edit_rejects_a_blank_type_or_parameter_and_a_negative_length()
    {
        Assert.Throws<ArgumentException>(() => FireReviewParameterEdit.OfText(" ", "P", "v"));
        Assert.Throws<ArgumentException>(() => FireReviewParameterEdit.OfText("T-1", " ", "v"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FireReviewParameterEdit.OfLength("T-1", "P", -0.01));
    }

    [Fact]
    public void A_row_rejects_impossible_measurements()
    {
        Assert.Throws<ArgumentException>(() => Row(uid: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => Row(dimensionCm: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Row(coverCm: -1));
    }

    [Fact]
    public void An_opening_row_derives_nothing_and_is_recognised_as_an_opening()
    {
        var door = Row("T-door", CandidateCategory.Door, dimensionCm: null, material: null, protection: true);

        Assert.True(door.IsOpening);
        Assert.Equal(FireRatingDerivationKind.NotDerivable, door.Derivation.Kind);
        Assert.True(door.ProvidedProtection);
    }

    /// <summary>
    /// 防火保護 is a Type parameter, so a row keeps 「沒有這個參數」 apart from 「有，但沒勾」: the
    /// first must write nothing, the second is an answer（否）.
    /// </summary>
    [Fact]
    public void An_opening_row_keeps_an_unbound_checkbox_apart_from_an_unticked_one()
    {
        Assert.Null(Row("T-door", CandidateCategory.Door, protection: null).ProvidedProtection);
        Assert.False(Row("T-door", CandidateCategory.Door, protection: false).ProvidedProtection);
    }

    /// <summary>
    /// The panel writes the tick to the Type, and unticking writes 0 rather than clearing — the
    /// review reads 0 as 否, and there is no way to put a Yes/No box back to「沒填」anyway.
    /// </summary>
    [Fact]
    public void A_checkbox_edit_names_the_type_and_carries_the_tick_as_a_yes_no()
    {
        var ticked = FireReviewParameterEdit.OfYesNo("T-door", FireProtectionParameters.Provided, true);

        Assert.Equal(FireReviewEditKind.YesNo, ticked.Kind);
        Assert.Equal("T-door", ticked.ElementUniqueId);
        Assert.True(ticked.YesNo);
        Assert.False(ticked.IsLength);
        Assert.Contains("是", ticked.ToString());

        Assert.False(FireReviewParameterEdit.OfYesNo("T-door", FireProtectionParameters.Provided, false).YesNo);
    }

    [Fact]
    public void A_checkbox_edit_may_not_target_the_write_back_parameter()
    {
        Assert.Throws<ArgumentException>(() =>
            FireReviewParameterEdit.OfYesNo("T-door", FireRatingParameters.Required, true));
    }

    // --- 帷幕嵌板（帷幕牆規格 §6、§12 步驟 5）-----------------------------------------------------

    /// <summary>
    /// A 帷幕嵌板 is the one opening that answers 設計防火時效 as well: 第79條第4項 and 第79條之3第2項
    /// measure the 交接帶 by the panels' own rating, so the batch panel has to let it be typed in.
    /// </summary>
    [Fact]
    public void A_curtain_panel_answers_both_the_rating_and_the_protection()
    {
        var panel = Row("T-panel", CandidateCategory.CurtainPanel);

        Assert.True(panel.IsOpening);
        Assert.True(panel.CarriesRating);
        Assert.True(panel.CarriesProtection);
    }

    [Fact]
    public void A_door_or_a_window_answers_the_protection_only()
    {
        foreach (var category in new[] { CandidateCategory.Door, CandidateCategory.Window })
        {
            var row = Row("T-" + category, category);
            Assert.False(row.CarriesRating);
            Assert.True(row.CarriesProtection);
        }
    }

    [Fact]
    public void A_structural_member_answers_the_rating_only()
    {
        foreach (var category in CandidateCategories.Members)
        {
            var row = Row("T-" + category, category);
            Assert.True(row.CarriesRating);
            Assert.False(row.CarriesProtection);
        }
    }

    /// <summary>
    /// 第71～73條 give a panel no dimensional threshold, so nothing is derived for it — the rating is
    /// the designer's, typed in, exactly like a 梁.
    /// </summary>
    [Fact]
    public void A_curtain_panel_derives_no_rating_of_its_own()
    {
        Assert.False(FireRatingDeriver.IsDerivable(CandidateCategory.CurtainPanel));
        Assert.False(Row("T-panel", CandidateCategory.CurtainPanel, dimensionCm: 8).WouldChangeRating);
    }
}
