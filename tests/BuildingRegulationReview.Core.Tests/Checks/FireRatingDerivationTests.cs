using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Checks;

/// <summary>
/// 結構材料＋斷面尺寸 → 防火時效, checked against the clause each threshold comes from
/// (建築技術規則建築設計施工編第71～73條). Boundary values matter most: the clauses say 「以上」,
/// so exactly 7cm is one hour, not none.
/// </summary>
public sealed class FireRatingDerivationTests
{
    private static FireRatingDerivation Derive(
        CandidateCategory category, string? material, double? dimensionCm, double? coverCm = null) =>
        FireRatingDeriver.Derive(
            category,
            StructuralMaterialText.Parse(material),
            dimensionCm is double d ? d / 100 : (double?)null,
            coverCm is double c ? c / 100 : (double?)null);

    [Theory]
    [InlineData("RC")]
    [InlineData("SRC")]
    [InlineData("鋼筋混凝土造")]
    [InlineData("鋼骨鋼筋混凝土造")]
    public void Wall_thickness_follows_article_72_and_73(string material)
    {
        Assert.Equal(120, Derive(CandidateCategory.Wall, material, 12).Minutes);
        Assert.Equal(120, Derive(CandidateCategory.Wall, material, 10).Minutes);
        Assert.Equal(60, Derive(CandidateCategory.Wall, material, 9.99).Minutes);
        Assert.Equal(60, Derive(CandidateCategory.Wall, material, 8).Minutes);
        Assert.Equal(60, Derive(CandidateCategory.Wall, material, 7).Minutes);

        var thin = Derive(CandidateCategory.Wall, material, 6.99);
        Assert.Equal(FireRatingDerivationKind.NotRated, thin.Kind);
        Assert.Null(thin.Minutes);
    }

    /// <summary>The example the tool is specified by: an 8cm RC wall is one hour.</summary>
    [Fact]
    public void Rc_wall_of_eight_centimetres_is_one_hour_under_article_73()
    {
        var derived = Derive(CandidateCategory.Wall, "RC", 8);

        Assert.Equal(FireRatingDerivationKind.Derived, derived.Kind);
        Assert.Equal(60, derived.Minutes);
        Assert.Equal("60 min", derived.ParameterText);
        Assert.Contains("第73條", derived.LegalReference);
    }

    [Theory]
    [InlineData("RC")]
    [InlineData("SRC")]
    public void Floor_thickness_follows_article_72_and_73(string material)
    {
        Assert.Equal(120, Derive(CandidateCategory.Floor, material, 10).Minutes);
        Assert.Equal(60, Derive(CandidateCategory.Floor, material, 7).Minutes);
        Assert.Equal(FireRatingDerivationKind.NotRated, Derive(CandidateCategory.Floor, material, 6).Kind);
        Assert.Contains("第72條第4款", Derive(CandidateCategory.Floor, material, 15).LegalReference);
    }

    /// <summary>第73條二(一) sets no minimum section, so an RC column is always rated at least one hour.</summary>
    [Theory]
    [InlineData("RC")]
    [InlineData("SRC")]
    public void Column_short_side_follows_article_71_72_and_73(string material)
    {
        Assert.Equal(180, Derive(CandidateCategory.Column, material, 40).Minutes);
        Assert.Equal(120, Derive(CandidateCategory.Column, material, 39.9).Minutes);
        Assert.Equal(120, Derive(CandidateCategory.Column, material, 25).Minutes);
        Assert.Equal(60, Derive(CandidateCategory.Column, material, 24.9).Minutes);
        Assert.Equal(60, Derive(CandidateCategory.Column, material, 10).Minutes);

        Assert.Contains("第71條", Derive(CandidateCategory.Column, material, 50).LegalReference);
        Assert.Contains("第72條", Derive(CandidateCategory.Column, material, 30).LegalReference);
        Assert.Contains("第73條", Derive(CandidateCategory.Column, material, 20).LegalReference);
    }

    [Fact]
    public void Steel_without_a_cover_thickness_is_not_derived_from_its_section()
    {
        var wall = Derive(CandidateCategory.Wall, "SC", dimensionCm: 30);

        Assert.Equal(FireRatingDerivationKind.CoverMissing, wall.Kind);
        Assert.Null(wall.Minutes);
        Assert.Contains(StructuralMaterialParameters.Cover, wall.Explanation);
    }

    [Fact]
    public void Steel_wall_and_floor_are_rated_by_cover_thickness()
    {
        Assert.Equal(120, Derive(CandidateCategory.Wall, "SC", 30, coverCm: 4).Minutes);
        Assert.Equal(60, Derive(CandidateCategory.Wall, "SC", 30, coverCm: 3).Minutes);
        Assert.Equal(FireRatingDerivationKind.NotRated, Derive(CandidateCategory.Wall, "SC", 30, coverCm: 2.9).Kind);

        Assert.Equal(120, Derive(CandidateCategory.Floor, "SC", 30, coverCm: 5).Minutes);
        Assert.Equal(60, Derive(CandidateCategory.Floor, "SC", 30, coverCm: 4).Minutes);
        Assert.Equal(FireRatingDerivationKind.NotRated, Derive(CandidateCategory.Floor, "SC", 30, coverCm: 3.9).Kind);
    }

    /// <summary>A thick steel section with thin cover is not rated by the section (第73條一(二)).</summary>
    [Fact]
    public void A_thick_steel_wall_with_thin_cover_is_not_two_hours()
    {
        var derived = Derive(CandidateCategory.Wall, "SC", dimensionCm: 40, coverCm: 3);

        Assert.Equal(60, derived.Minutes);
        Assert.Contains("第73條", derived.LegalReference);
    }

    [Fact]
    public void Steel_column_needs_both_cover_and_short_side_for_the_higher_ratings()
    {
        Assert.Equal(180, Derive(CandidateCategory.Column, "SC", 40, coverCm: 9).Minutes);
        // 9cm of cover but a 30cm section misses 第71條's 40cm short side, so it falls to 第72條.
        Assert.Equal(120, Derive(CandidateCategory.Column, "SC", 30, coverCm: 9).Minutes);
        // 5cm of cover on a 20cm section misses 第72條's 25cm short side, so it falls to 第73條.
        Assert.Equal(60, Derive(CandidateCategory.Column, "SC", 20, coverCm: 5).Minutes);
        Assert.Equal(FireRatingDerivationKind.NotRated, Derive(CandidateCategory.Column, "SC", 40, coverCm: 3.9).Kind);
    }

    /// <summary>第71～73條 state the 樑 requirement with no dimensional threshold, so nothing is derived.</summary>
    [Fact]
    public void Beams_are_never_derived()
    {
        var beam = Derive(CandidateCategory.StructuralFraming, "RC", 60);

        Assert.Equal(FireRatingDerivationKind.NotDerivable, beam.Kind);
        Assert.Contains("樑", beam.Explanation);
        Assert.False(FireRatingDeriver.IsDerivable(CandidateCategory.StructuralFraming));
        Assert.DoesNotContain(CandidateCategory.StructuralFraming, FireRatingDeriver.DerivableCategories);
    }

    [Fact]
    public void Openings_are_never_derived()
    {
        Assert.Equal(FireRatingDerivationKind.NotDerivable, Derive(CandidateCategory.Door, "RC", 10).Kind);
        Assert.Equal(FireRatingDerivationKind.NotDerivable, Derive(CandidateCategory.Window, "RC", 10).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("磚造")]
    [InlineData("木構造")]
    public void An_unset_or_unknown_material_derives_nothing(string? material)
    {
        var derived = Derive(CandidateCategory.Wall, material, 20);

        Assert.Equal(FireRatingDerivationKind.MaterialMissing, derived.Kind);
        Assert.Null(derived.Minutes);
        Assert.Contains(StructuralMaterialParameters.Material, derived.Explanation);
    }

    [Fact]
    public void A_missing_dimension_derives_nothing()
    {
        Assert.Equal(FireRatingDerivationKind.DimensionMissing, Derive(CandidateCategory.Wall, "RC", null).Kind);
        Assert.Equal(FireRatingDerivationKind.DimensionMissing, Derive(CandidateCategory.Floor, "RC", null).Kind);
        Assert.Equal(FireRatingDerivationKind.DimensionMissing, Derive(CandidateCategory.Column, "RC", null).Kind);
    }

    [Theory]
    [InlineData("rc", StructuralMaterial.ReinforcedConcrete)]
    [InlineData(" SRC ", StructuralMaterial.SteelReinforcedConcrete)]
    [InlineData("sc", StructuralMaterial.Steel)]
    [InlineData("鋼骨造", StructuralMaterial.Steel)]
    public void Material_text_is_read_case_and_space_insensitively(string text, StructuralMaterial expected) =>
        Assert.Equal(expected, StructuralMaterialText.Parse(text));

    [Fact]
    public void Every_offered_material_round_trips_through_its_code()
    {
        foreach (var material in StructuralMaterialText.All)
            Assert.Equal(material, StructuralMaterialText.Parse(StructuralMaterialText.Code(material)));
    }

    /// <summary>The derived text must be readable by the parser the review itself uses (spec 11.5 step 3).</summary>
    [Theory]
    [InlineData(CandidateCategory.Wall, "RC", 8, 60)]
    [InlineData(CandidateCategory.Wall, "RC", 12, 120)]
    [InlineData(CandidateCategory.Column, "SRC", 45, 180)]
    public void The_written_text_parses_back_to_the_same_rating(
        CandidateCategory category, string material, double dimensionCm, double expected)
    {
        var derived = Derive(category, material, dimensionCm);

        var parsed = FireRatingText.Parse(derived.ParameterText, FireRatingUnit.Minute);

        Assert.Equal(ProvidedFireRatingKind.Rated, parsed.Kind);
        Assert.Equal(expected, parsed.Minutes);
    }
}
