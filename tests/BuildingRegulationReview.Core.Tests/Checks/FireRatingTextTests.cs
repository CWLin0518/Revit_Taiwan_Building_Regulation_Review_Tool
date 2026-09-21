using System;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Reviews;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Checks;

public sealed class FireRatingTextTests
{
    [Theory]
    [InlineData("60", 60)]
    [InlineData(" 60 ", 60)]
    [InlineData("0", 0)]
    [InlineData("60 min", 60)]
    [InlineData("60min", 60)]
    [InlineData("60 MIN", 60)]
    [InlineData("90 minutes", 90)]
    [InlineData("60分", 60)]
    [InlineData("120分鐘", 120)]
    [InlineData("1 hr", 60)]
    [InlineData("1HR", 60)]
    [InlineData("1.5h", 90)]
    [InlineData("2 hours", 120)]
    [InlineData("2小時", 120)]
    [InlineData("1時", 60)]
    [InlineData("１２０", 120)]
    [InlineData("１．５ｈｒ", 90)]
    [InlineData("一小時", 60)]
    [InlineData("兩小時", 120)]
    [InlineData("三小時", 180)]
    [InlineData("一小時半", 90)]
    [InlineData("1小時半", 90)]
    [InlineData("半小時", 30)]
    [InlineData("24 hr", 1440)]
    public void A_single_rating_is_read_in_minutes(string text, double minutes)
    {
        var rating = FireRatingText.Parse(text);

        Assert.Equal(ProvidedFireRatingKind.Rated, rating.Kind);
        Assert.Equal(minutes, rating.Minutes!.Value, 9);
        Assert.Equal(text.Trim(), rating.RawText);
        Assert.Equal(ReviewValue.Quantity(minutes, ReviewUnit.Minute), rating.Value);
    }

    [Theory]
    [InlineData(FireRatingUnit.Minute, 2)]
    [InlineData(FireRatingUnit.Hour, 120)]
    public void A_bare_number_takes_the_declared_unit_but_an_explicit_unit_wins(FireRatingUnit unit, double minutes)
    {
        Assert.Equal(minutes, FireRatingText.Parse("2", unit).Minutes);
        Assert.Equal(60, FireRatingText.Parse("60 min", unit).Minutes);
        Assert.Equal(60, FireRatingText.Parse("1 hr", unit).Minutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_value_is_missing_not_zero(string? text)
    {
        var rating = FireRatingText.Parse(text);

        Assert.Equal(ProvidedFireRatingKind.Missing, rating.Kind);
        Assert.Null(rating.Minutes);
        Assert.Null(rating.Value);
    }

    [Theory]
    [InlineData("耐燃")]
    [InlineData("RC 牆")]
    [InlineData("-30")]
    [InlineData("60 m")]
    [InlineData("六十分")]
    [InlineData("一")]
    [InlineData("60分半")]
    [InlineData("25 hr")]
    [InlineData("F60")]
    public void Anything_that_is_not_one_rating_is_unreadable_with_a_reason(string text)
    {
        var rating = FireRatingText.Parse(text);

        Assert.Equal(ProvidedFireRatingKind.Unreadable, rating.Kind);
        Assert.Null(rating.Minutes);
        Assert.Equal(text, rating.RawText);
        Assert.False(string.IsNullOrWhiteSpace(rating.Reason));
    }

    [Fact]
    public void Unreadable_reasons_name_the_problem()
    {
        Assert.Contains("負值", FireRatingText.Parse("-30").Reason);
        Assert.Contains("24 小時", FireRatingText.Parse("25 hr").Reason);
        Assert.Contains("小時", FireRatingText.Parse("一").Reason);
    }

    [Theory]
    [InlineData("1hr/2hr")]
    [InlineData("1 hr ／ 2 hr")]
    [InlineData("60~120")]
    [InlineData("60-120 min")]
    [InlineData("一小時、兩小時")]
    [InlineData("牆 1hr + 被覆 1hr")]
    public void Several_ratings_in_one_value_are_a_composite_construction(string text)
    {
        var rating = FireRatingText.Parse(text);

        Assert.Equal(ProvidedFireRatingKind.Undeterminable, rating.Kind);
        Assert.Null(rating.Minutes);
        Assert.Contains("複合構造", rating.Reason);
    }

    [Theory]
    [InlineData(60, FireRatingUnit.Minute, 60)]
    [InlineData(1.5, FireRatingUnit.Hour, 90)]
    [InlineData(0, FireRatingUnit.Minute, 0)]
    public void A_numeric_parameter_is_converted_by_its_unit(double value, FireRatingUnit unit, double minutes)
    {
        var rating = ProvidedFireRating.FromNumber(value, unit);

        Assert.True(rating.IsRated);
        Assert.Equal(minutes, rating.Minutes);
    }

    [Theory]
    [InlineData(-1, FireRatingUnit.Minute)]
    [InlineData(double.NaN, FireRatingUnit.Minute)]
    [InlineData(double.PositiveInfinity, FireRatingUnit.Hour)]
    [InlineData(25, FireRatingUnit.Hour)]
    public void An_impossible_numeric_parameter_is_unreadable(double value, FireRatingUnit unit)
    {
        Assert.Equal(ProvidedFireRatingKind.Unreadable, ProvidedFireRating.FromNumber(value, unit).Kind);
    }

    [Fact]
    public void Ratings_print_minutes_and_hours()
    {
        Assert.Equal("30 min", FireRatingText.Format(30));
        Assert.Equal("90 min（1.5 小時）", FireRatingText.Format(90));
        Assert.Equal("60 min（1 小時）", ProvidedFireRating.Rated(60).ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => ProvidedFireRating.Rated(-1));
        Assert.Throws<ArgumentException>(() => ProvidedFireRating.Unreadable("x", " "));
    }

    [Fact]
    public void The_required_rating_parameter_is_never_a_design_value_source()
    {
        Assert.NotEqual(FireRatingParameters.Provided, FireRatingParameters.Required);
        Assert.True(FireRatingParameters.IsRequiredParameter(" bcr_requiredfirerating "));
        Assert.False(FireRatingParameters.IsRequiredParameter(FireRatingParameters.Provided));

        var ex = Assert.Throws<ArgumentException>(() =>
            new TypeFireRating("type-1", ProvidedFireRating.Rated(60), FireRatingParameters.Required));
        Assert.Contains(FireRatingParameters.Required, ex.Message);
        Assert.Equal("Fire Rating", new TypeFireRating("type-1", ProvidedFireRating.Rated(60), " Fire Rating ").Source);
    }
}
